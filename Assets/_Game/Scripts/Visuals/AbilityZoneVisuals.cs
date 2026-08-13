using CluckWars.Gameplay;
using Fusion;   // TickTimer / NetworkRunner, read only. No CluckWars.Logging import here, so no LogLevel alias is needed.
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Gives a placed <see cref="AbilityZone"/> (Feather Trap, Root Egg) the two things
    /// FEEDBACK.md §7 says it is missing: a visible <b>edge</b> (case 22 — "a placed zone
    /// exists and where its edge is") and a visible <b>expiry</b> (case 23 — "a zone is
    /// about to expire"). Today a zone is an invisible sphere you find out about by
    /// walking into it.
    /// </summary>
    /// <remarks>
    /// <b>The ring is drawn at exactly <see cref="AbilityZone.TriggerRadius"/></b> — the
    /// same accessor the gameplay overlap uses — so "where its edge is" is not an artist's
    /// approximation of the trigger, it *is* the trigger. That accessor already resolves
    /// the per-spawn <c>NetworkedRadius</c> override against the prefab default, so a
    /// zone spawned at a non-default radius draws correctly with no extra plumbing.
    ///
    /// <b>Colour mapping.</b> <c>ZoneEffect</c> has exactly two members today and each
    /// maps to the canonical colour of the state it inflicts, so a Feather Trap's ring is
    /// the same cyan as the slow ring it will put on you and a Root Egg's is the same
    /// green as the root ring — the zone and its consequence are never two different
    /// colours to learn:
    /// <list type="bullet">
    ///   <item><c>ZoneEffect.Slow</c> → <see cref="FeedbackTuning.CanonicalSlowColor"/></item>
    ///   <item><c>ZoneEffect.Root</c> → <see cref="FeedbackTuning.CanonicalRootColor"/></item>
    /// </list>
    /// Any future member falls through to <see cref="FeedbackTuning.CanonicalKnockColor"/>
    /// (white) — deliberately visible-but-unstyled, so a new zone type ships looking
    /// unfinished rather than shipping invisible.
    ///
    /// <b>Local observation only.</b> Everything read here is already replicated
    /// (<c>LifetimeTimer</c>, <c>Consumed</c>, <c>Effect</c>, <c>NetworkedRadius</c>), and
    /// is read in <c>LateUpdate</c>. No RPCs, no new networked properties, nothing
    /// written. The drain reuses <see cref="DrainRing"/> — the project's single arc
    /// implementation, shared with the §5.1 status arc and the case-30 buff ring — so its
    /// geometry is regenerated on the same
    /// <see cref="FeedbackTuning.StatusArcDrainUpdateHz"/> budget and a draining circle
    /// means the same thing to the player wherever it appears.
    ///
    /// <b>Maestro:</b> add to the <c>AbilityZone</c> prefab (the one assigned to
    /// <c>PrefabRegistrySO.AbilityZone</c>). No Inspector wiring required.
    /// </remarks>
    [RequireComponent(typeof(AbilityZone))]
    public sealed class AbilityZoneVisuals : MonoBehaviour
    {
        /// <summary>Draw height. Matches <c>ControlStateVFX</c>'s ground plane so a zone
        /// ring and a chicken's status ring sit on the same visual floor.</summary>
        private const float GroundY = 0.05f;

        private const float TrackWidth = 0.07f;
        private const float ArcWidth   = 0.12f;

        /// <summary>Base alpha of the zone's draining arc. Higher than a chicken's status
        /// ring (which peaks at 0.80) because a zone has no body, no nameplate and no
        /// overlay to help it — the ring is the entire object as far as the player is
        /// concerned.</summary>
        private const float RingAlpha = 0.85f;

        /// <summary>Slow breathing pulse so a placed zone reads as live rather than as a
        /// decal baked into the arena. Deliberately slower than the status ring's
        /// ~1.43 Hz: a hazard sitting on the floor should not compete for urgency with a
        /// control state actively running on a chicken.</summary>
        private const float PulseRadPerSecond = 3.2f;
        private const float PulseAmplitude    = 0.12f;

        private AbilityZone _zone;
        private DrainRing   _ring;

        /// <summary>Longest lifetime this peer has ever observed on this zone. The zone's
        /// authored duration is not replicated (and this stage may not add it), so the
        /// drain is measured against the maximum remaining time seen since the zone was
        /// first rendered — identical to the approach <c>ControlStateVFX</c> uses for stun
        /// and root, and correct from the first frame because a zone is observed from its
        /// own spawn.</summary>
        private float _observedMaxLifetime;

        private void Awake()
        {
            _zone = GetComponent<AbilityZone>();
            _ring = new DrainRing(transform, "ZoneEdgeRing", TelegraphShapes.BuildLineMaterial(),
                                  TrackWidth, ArcWidth);
        }

        private void LateUpdate()
        {
            if (_zone == null || _ring == null) return;

            // Null-safe against the window where the component exists but the
            // NetworkObject has not been spawned (or is already despawning): the
            // networked timer cannot be read without a live Runner.
            var obj = _zone.Object;
            if (obj == null || !obj.IsValid || _zone.Runner == null)
            {
                _ring.Hide();
                return;
            }

            // Consumed zones are despawned by their authority on the next tick; hide
            // immediately so a spent Root Egg never sits on the floor still advertising a
            // trigger that is gone.
            if (_zone.Consumed)
            {
                _ring.Hide();
                return;
            }

            float remaining = _zone.LifetimeTimer.ExpiredOrNotRunning(_zone.Runner)
                ? 0f
                : (_zone.LifetimeTimer.RemainingTime(_zone.Runner) ?? 0f);

            if (remaining <= 0f)
            {
                _ring.Hide();
                return;
            }

            if (remaining > _observedMaxLifetime) _observedMaxLifetime = remaining;
            float fraction = _observedMaxLifetime > 0f
                ? Mathf.Clamp01(remaining / _observedMaxLifetime)
                : 1f;

            Color c = ColorFor(_zone.Effect);
            c.a = RingAlpha * (1f - PulseAmplitude + PulseAmplitude * Mathf.Sin(Time.time * PulseRadPerSecond));

            Vector3 center = transform.position;
            center.y = GroundY;
            _ring.Show(center, _zone.TriggerRadius, c, fraction, drain: true);
        }

        private static Color ColorFor(ZoneEffect effect)
        {
            switch (effect)
            {
                case ZoneEffect.Slow: return FeedbackTuning.CanonicalSlowColor;
                case ZoneEffect.Root: return FeedbackTuning.CanonicalRootColor;
                default:              return FeedbackTuning.CanonicalKnockColor;
            }
        }
    }
}
