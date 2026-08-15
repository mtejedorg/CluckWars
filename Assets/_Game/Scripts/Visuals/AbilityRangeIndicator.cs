using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

// v0.3 ability feedback: ground range ring + cast-flash (see PR / STATE.md).
// v0.6 Stage 3: the cast flash became shape-aware and hit/whiff-aware (FEEDBACK.md §3.1, §3.3).
// v0.7: the range ring moved out to AbilitySlotOverlay, which draws every equipped slot's
//       real aim shape instead of one circle at the widest IndicatorRange. This file is now
//       the cast flash and nothing else — the class name is kept only to preserve the
//       Chicken.prefab component reference (renaming it would break the prefab's script GUID
//       binding for no behavioural gain).
namespace CluckWars.Visuals
{
    /// <summary>
    /// The <b>cast flash</b>: local, on-ground impact confirmation, drawn on every peer.
    /// Two LineRenderers on the flat arena floor:
    ///
    /// <list type="bullet">
    ///   <item><b>Cast flash</b>: an expanding, fading outline at the ability's <i>true aim
    ///   shape</i> the moment it fires — cone arc for a cone, offset circle for a placed
    ///   zone, landing ring for a jump, self-ring for a self-buff. Accent-tinted on a hit;
    ///   desaturated and dimmed on a whiff (§3.3), so "it connected" and "it missed" can
    ///   never look alike.</item>
    ///   <item><b>Flash stalk</b>: the connector from the caster out to an offset
    ///   shape's centre, so a placed zone reads as <i>theirs</i>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>The persistent range ring this class used to own has been retired</b> in favour of
    /// <see cref="AbilitySlotOverlay"/>, which draws every equipped slot's real aim shape
    /// instead of one coarse circle at the widest <c>IndicatorRange</c>. The ring special-cased
    /// the same <c>RequiresEnemyInRange</c> / <c>IsUsable</c> state the overlay has to compute
    /// anyway, and two systems deriving "is a target in range" from the same state is exactly
    /// the drift FEEDBACK.md §4 exists to prevent. The behavioural upgrade: a ready Warrior
    /// carrying both Cluck Shock and Sneaky Steal used to get ONE brightened ring (widest-range
    /// tie-break); now both shapes brighten independently when a rival enters each.
    ///
    /// Purely local VFX driven by polling replicated state (<c>LastCastEventId</c>,
    /// <c>LastCastHitCount</c>, <c>ChargingSlot</c>) in <c>LateUpdate</c> — no RPCs,
    /// consistent with <see cref="ChickenVFX"/>. Shape geometry is shared with the telegraph
    /// via <see cref="TelegraphShapes"/> so the preview a player aimed with and the flash they
    /// get back are the same outline.
    /// <b>Maestro</b>: add to the Chicken prefab; no Inspector wiring needed (the lines
    /// are built procedurally in <c>Awake</c>).
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class AbilityRangeIndicator : MonoBehaviour
    {
        [Tooltip("Seconds the cast-flash ring takes to expand and fade out.")]
        [Min(0.05f)] [SerializeField] private float _flashDuration = 0.35f;

        private AbilityController _abilities;

        private LineRenderer _flash;       // expanding cast-confirm outline (all peers)
        private LineRenderer _flashStalk;  // caster → offset-shape centre connector

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
            _abilities = GetComponent<AbilityController>();

            var mat = TelegraphShapes.BuildLineMaterial();
            _flash      = TelegraphShapes.BuildLine(transform, "AbilityCastFlash", mat, 0.11f, loop: true);
            _flashStalk = TelegraphShapes.BuildLine(transform, "AbilityCastFlashStalk", mat, 0.05f, loop: false);
            _flash.enabled      = false;
            _flashStalk.enabled = false;
        }

        private void LateUpdate()
        {
            ObserveCast();
            AnimateFlash();
        }

        /// <summary>The flash lives on children of this chicken, so it dies with it — but a
        /// component disabled without being destroyed would strand a half-expanded ring on
        /// screen forever. Same precedent as <c>RivalIndicator.OnDisable</c>.</summary>
        private void OnDisable()
        {
            _flashTimer = -1f;
            if (_flash != null && _flash.enabled) _flash.enabled = false;
            if (_flashStalk != null && _flashStalk.enabled) _flashStalk.enabled = false;
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
            // actually resolves a hit AND which had something to aim at in the first place:
            // self-buffs have no aim shape to gather from, a placed zone (Root Egg /
            // Feather Trap) hits later in AbilityZone's tick, and a gap-closer fires on
            // purpose with nobody in the lane — all three report 0 by construction and must
            // keep their accent ring. Every exemption lives in one place,
            // AbilityBaseSO.ZeroHitsIsAWhiff, which is ReportsCastHits narrowed by
            // RequiresEnemyInRange.
            bool whiff = ability.ZeroHitsIsAWhiff && _abilities.LastCastHitCount == 0;

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
    }
}
