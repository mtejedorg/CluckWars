using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

// v0.3 ability feedback: ground range ring + cast-flash (see PR / STATE.md).
// v0.6 Stage 3: the cast flash became shape-aware and hit/whiff-aware (FEEDBACK.md §3.1, §3.3).
namespace CluckWars.Visuals
{
    /// <summary>
    /// Local, on-ground feedback for ability range and impact. Three LineRenderers on the
    /// flat arena floor:
    ///
    /// <list type="bullet">
    ///   <item><b>Range ring</b> (local player only): a persistent circle around the
    ///   chicken at the reach of its range-gated abilities (Peck, Cluck Shock,
    ///   Sneaky Steal). Dim while no valid target is inside; brightens + thickens
    ///   the instant an enemy enters range — the same ready/usable signal the HUD
    ///   button shows, but in the world so the player understands *why* the button
    ///   is grey. Yields to <see cref="AbilityTelegraph"/>'s preview while that same
    ///   slot is being aimed, so the two rings never stack.</item>
    ///   <item><b>Cast flash</b> (every peer): an expanding, fading outline at the
    ///   ability's <i>true aim shape</i> the moment it fires — cone arc for a cone,
    ///   offset circle for a placed zone, landing ring for a jump, self-ring for a
    ///   self-buff. Accent-tinted on a hit; desaturated and dimmed on a whiff
    ///   (§3.3), so "it connected" and "it missed" can never look alike.</item>
    ///   <item><b>Flash stalk</b>: the connector from the caster out to an offset
    ///   shape's centre, so a placed zone reads as <i>theirs</i>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Purely local VFX driven by polling replicated state (<c>LastCastEventId</c>,
    /// <c>LastCastHitCount</c>, <c>ChargingSlot</c>, cooldowns, rival positions) in
    /// <c>LateUpdate</c> — no RPCs, consistent with <see cref="ChickenVFX"/>. Shape
    /// geometry is shared with the telegraph via <see cref="TelegraphShapes"/> so the
    /// preview a player aimed with and the flash they get back are the same outline.
    /// <b>Maestro</b>: add to the Chicken prefab; no Inspector wiring needed (the lines
    /// are built procedurally in <c>Awake</c>).
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class AbilityRangeIndicator : MonoBehaviour
    {
        private const int   Segments = TelegraphShapes.CircleSegments;
        private const float GroundY  = TelegraphShapes.GroundY; // just above the y=0 arena plane (avoid z-fighting)

        [Tooltip("Seconds the cast-flash ring takes to expand and fade out.")]
        [Min(0.05f)] [SerializeField] private float _flashDuration = 0.35f;

        private ChickenController _controller;
        private AbilityController _abilities;

        private LineRenderer _ring;        // persistent range ring (local player only)
        private LineRenderer _flash;       // expanding cast-confirm outline (all peers)
        private LineRenderer _flashStalk;  // caster → offset-shape centre connector

        private Vector3[] _dirs;       // precomputed unit circle directions (range ring only)
        private Vector3[] _ringBuf;
        private Vector3[] _flashBuf;   // grown by TelegraphShapes only when the shape changes
        private readonly Vector3[] _stalkBuf = new Vector3[2];

        // Cast-flash animation state, snapshotted at the moment of the cast so the flash
        // stays where the ability actually landed instead of trailing the chicken.
        private float   _flashTimer = -1f;
        private Color   _flashColor = Color.white;
        private float   _flashAlphaScale = 1f;
        private Vector3 _flashOrigin;
        private Vector3 _flashForward = Vector3.forward;
        private AbilityAimShape _flashShape = AbilityAimShape.None;
        private float _flashRadius, _flashOffset, _flashConeAngle = 360f;

        // Replicated one-shot observation (same pattern as ControlStateVFX.UpdateKnockback).
        private byte _lastCastEventId;
        private bool _castInitialized;

        // Last ability seen charging, as a fallback for reading the fired ability off a
        // very short activation window — see ObserveCast.
        private AbilityBaseSO _lastChargingAbility;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _abilities  = GetComponent<AbilityController>();

            // Precompute the unit circle once; the persistent range ring reuses it.
            _dirs    = new Vector3[Segments];
            _ringBuf = new Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float a = (i / (float)Segments) * Mathf.PI * 2f;
                _dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }

            var mat = TelegraphShapes.BuildLineMaterial();
            _ring       = TelegraphShapes.BuildLine(transform, "AbilityRangeRing", mat, 0.06f, loop: true);
            _flash      = TelegraphShapes.BuildLine(transform, "AbilityCastFlash", mat, 0.11f, loop: true);
            _flashStalk = TelegraphShapes.BuildLine(transform, "AbilityCastFlashStalk", mat, 0.05f, loop: false);
            _ring.enabled       = false;
            _flash.enabled      = false;
            _flashStalk.enabled = false;
        }

        private void LateUpdate()
        {
            UpdateRangeRing();
            ObserveCast();
            AnimateFlash();
        }

        /// <summary>Networked properties are only safe to read between Spawned and Despawned.</summary>
        private bool NetworkReady
        {
            get
            {
                if (_abilities == null) return false;
                var obj = _abilities.Object;
                return obj != null && obj.IsValid;
            }
        }

        // ---- Persistent range ring (local player only) ------------------------

        private void UpdateRangeRing()
        {
            // Only the controlling player gets the aiming aid; rivals/bots don't.
            if (!NetworkReady || _controller == null || !_controller.HasInputAuthority)
            {
                if (_ring.enabled) _ring.enabled = false;
                return;
            }

            // Pick the ability whose range to draw: prefer a ready, range-gated
            // ability that currently has a target inside (ring lights up); else the
            // widest ready range-gated ability (dim, "get closer"); else nothing.
            float bestRange = 0f;
            Color bestAccent = Color.white;
            int   bestSlot = AbilityController.InvalidSlot;
            bool  targetInRange = false;

            for (int slot = 0; slot < 3; slot++)
            {
                var ab = SlotAbility(slot);
                if (ab == null || !ab.RequiresEnemyInRange || ab.IndicatorRange <= 0f) continue;
                if (!_abilities.IsReady(slot)) continue;

                bool hasTarget = ab.IsUsable(_controller);
                // A ready ability with a target always wins; otherwise track the widest.
                if (hasTarget && !targetInRange)
                {
                    targetInRange = true;
                    bestRange = ab.IndicatorRange;
                    bestAccent = ab.AccentColor;
                    bestSlot = slot;
                }
                else if (hasTarget && ab.IndicatorRange > bestRange)
                {
                    bestRange = ab.IndicatorRange;
                    bestAccent = ab.AccentColor;
                    bestSlot = slot;
                }
                else if (!targetInRange && ab.IndicatorRange > bestRange)
                {
                    bestRange = ab.IndicatorRange;
                    bestAccent = ab.AccentColor;
                    bestSlot = slot;
                }
            }

            if (bestRange <= 0f)
            {
                if (_ring.enabled) _ring.enabled = false;
                return;
            }

            // While that same slot is being aimed, AbilityTelegraph is already drawing the
            // ability's exact shape — a second, coarser circle at IndicatorRange on top of
            // it would just read as two conflicting areas. Yield.
            if (_abilities.ChargingSlot != 0 && _abilities.ChargingSlot == bestSlot + 1)
            {
                if (_ring.enabled) _ring.enabled = false;
                return;
            }

            // In range → bright + thicker; out of range → dim + thin. Mirrors the
            // HUD button colour/grey state so the relationship is learnable.
            Color c = bestAccent;
            c.a = targetInRange ? 0.60f : 0.16f;
            _ring.startColor = _ring.endColor = c;
            _ring.widthMultiplier = targetInRange ? 0.10f : 0.05f;

            WriteCircle(_ring, _ringBuf, _controller.transform.position, bestRange);
            if (!_ring.enabled) _ring.enabled = true;
        }

        // ---- Cast flash (all peers) -------------------------------------------

        /// <summary>
        /// Fires the flash off the replicated <c>LastCastEventId</c> one-shot rather than an
        /// <c>ActiveSlot</c> transition. The old edge-detect missed two real cases: an
        /// ability whose active window closed inside a single frame, and two back-to-back
        /// casts of the <i>same</i> slot (the slot never returned to Invalid in between, so
        /// the second cast drew nothing). Baseline is seeded on first observation so a late
        /// joiner inheriting a non-zero id doesn't flash a cast that already happened —
        /// same guard <c>ControlStateVFX.UpdateKnockback</c> uses for knockbacks.
        /// </summary>
        private void ObserveCast()
        {
            if (!NetworkReady) return;

            // Remember what was last being aimed: TryActivate clears ChargingSlot in the same
            // tick it fires, so by the time this observes the event the charge is gone, and
            // ActiveSlot may also already have expired for a very short-duration ability.
            if (_abilities.ChargingSlot != 0)
            {
                var charging = _abilities.ChargingAbility;
                if (charging != null) _lastChargingAbility = charging;
            }

            byte id = _abilities.LastCastEventId;
            if (!_castInitialized)
            {
                _castInitialized = true;
                _lastCastEventId = id;
                return;
            }
            if (id == _lastCastEventId) return;
            _lastCastEventId = id;

            var ability = _abilities.ActiveAbility ?? _lastChargingAbility;
            if (ability == null) return;

            TelegraphShapes.Resolve(ability, out _flashShape, out _flashRadius,
                                    out _flashOffset, out _flashConeAngle);

            // Snapshot the pose: a placed zone or a cone must flash where it was cast.
            _flashOrigin  = transform.position;
            _flashForward = transform.forward;

            // …except when the cast was aimed from a pose that no longer exists. Flying Peck
            // resolves its lane BEFORE its teleport, and this observation runs after the
            // landing on every peer, so drawing the authored shape here would put a 5 m lane
            // a full jump length past where it actually swept. Degrade to the caster
            // self-ring rather than flashing a confident lie — the player still gets the
            // cast beat and the §3.3 hit/whiff colouring below, just on their own body.
            // See AbilityBaseSO.CastPoseIsUnreconstructable.
            if (ability.CastPoseIsUnreconstructable)
            {
                _flashShape     = AbilityAimShape.None;
                _flashRadius    = FeedbackTuning.SelfRingRadius;
                _flashOffset    = 0f;
                _flashConeAngle = 360f;
            }

            // §3.3 hit vs whiff. A zero hit count is only a whiff for abilities whose cast
            // actually resolves a hit: self-buffs have no aim shape to gather from, and a
            // placed zone (Root Egg / Feather Trap) hits later, in AbilityZone's tick, so
            // both report 0 by construction and must keep their accent ring. Both exemptions
            // live in one place — AbilityBaseSO.ReportsCastHits.
            bool whiff = ability.ReportsCastHits && _abilities.LastCastHitCount == 0;

            _flashColor      = whiff ? Desaturate(ability.AccentColor, FeedbackTuning.WhiffRingSaturationMultiplier)
                                     : ability.AccentColor;
            _flashAlphaScale = whiff ? FeedbackTuning.WhiffRingAlphaMultiplier : 1f;
            _flashTimer      = 0f;
        }

        private void AnimateFlash()
        {
            if (_flashTimer < 0f) return;

            _flashTimer += Time.deltaTime;
            float t = _flashTimer / _flashDuration;
            if (t >= 1f)
            {
                _flashTimer = -1f;
                if (_flash.enabled) _flash.enabled = false;
                if (_flashStalk.enabled) _flashStalk.enabled = false;
                return;
            }

            // Ease-out expansion from 30% to full radius; alpha fades 0.7 → 0, scaled down
            // further on a whiff.
            float eased  = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(_flashRadius * 0.3f, _flashRadius, eased);
            Color c = _flashColor;
            c.a = (1f - t) * 0.7f * _flashAlphaScale;
            _flash.startColor = _flash.endColor = c;

            TelegraphShapes.Apply(_flash, ref _flashBuf, _flashShape, _flashOrigin, _flashForward,
                                  radius, _flashOffset, _flashConeAngle);
            if (!_flash.enabled) _flash.enabled = true;

            if (TelegraphShapes.NeedsStalk(_flashShape, _flashOffset))
            {
                _flashStalk.startColor = _flashStalk.endColor = c;
                TelegraphShapes.ApplyStalk(_flashStalk, _stalkBuf, _flashOrigin,
                    AbilityAim.ShapeCenter(_flashShape, _flashOrigin, _flashForward, _flashOffset));
                if (!_flashStalk.enabled) _flashStalk.enabled = true;
            }
            else if (_flashStalk.enabled)
            {
                _flashStalk.enabled = false;
            }
        }

        // ---- Helpers ----------------------------------------------------------

        /// <summary>Pulls a colour toward grey by scaling its HSV saturation, preserving hue,
        /// value and alpha — §3.3's "grey (not accent) cast ring".</summary>
        private static Color Desaturate(Color c, float saturationMultiplier)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Color result = Color.HSVToRGB(h, Mathf.Clamp01(s * saturationMultiplier), v);
            result.a = c.a;
            return result;
        }

        private AbilityBaseSO SlotAbility(int slot) =>
            slot == 0 ? _abilities.Slot0 : (slot == 1 ? _abilities.Slot1 : _abilities.Slot2);

        private void WriteCircle(LineRenderer lr, Vector3[] buf, Vector3 center, float radius)
        {
            center.y = GroundY;
            for (int i = 0; i < Segments; i++)
                buf[i] = center + _dirs[i] * radius;
            if (lr.positionCount != Segments) lr.positionCount = Segments;
            lr.SetPositions(buf);
        }
    }
}
