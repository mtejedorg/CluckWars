using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only motion for a chicken's per-class model: pushes the <see cref="Animator"/>
    /// parameters that drive the skeletal clips, and adds the few procedural offsets a fixed-length
    /// clip cannot express. Runs on every client (state authority and proxies alike) — animation is
    /// never networked. Sibling component on the Chicken prefab.
    /// </summary>
    /// <remarks>
    /// <b>The skeletal / procedural split.</b> The class rigs
    /// (<c>Assets/Generated/*_chicken_rigged.fbx</c>) each ship five clips — Idle, Walk, Cast, Hit,
    /// Stunned — bound at spawn through a runtime <c>AnimatorOverrideController</c> (see
    /// <c>ChickenController.BindSkeletalAnimation</c>). The clips own <i>articulation</i>: legs,
    /// wings, neck, spine, tail. This component owns only what a clip authored at a fixed length,
    /// in place, cannot know:
    /// <list type="bullet">
    /// <item>the forward lean, a function of the chicken's live speed;</item>
    /// <item>the banking roll, a function of its live yaw rate.</item>
    /// </list>
    /// Anything that reads as body articulation belongs in the clip. If the walk reads flat, fix
    /// the Walk clip — do not add a second oscillator here, because an independent-frequency
    /// sinusoid laid over a stride cycle beats against it and reads as a limp.
    ///
    /// <b>The single-write rule.</b> Effects are composed additively into local accumulators and
    /// written to the transform exactly ONCE per frame at the bottom of each path. Do not add an
    /// effect that assigns <c>localPosition</c>/<c>localRotation</c>/<c>localScale</c> directly —
    /// the moment two effects each assign, they fight and the last one silently wins.
    ///
    /// <b>The feet-on-ground invariant.</b> On the skeletal path this component contributes only
    /// non-negative Y <i>position</i> offsets and <i>no vertical scale at all</i>, so it can never
    /// sink the model below the grounded rest pose that <c>ChickenController.AttachClassModel</c>
    /// computed. Vertical articulation is the clips' business, and the clips are authored with the
    /// rig's feet at the origin. That makes the invariant structural rather than something to
    /// remember: there is no code path here that can lower a chicken.
    ///
    /// <b>Root vs model.</b> Only the child model transform is touched. The networked root is
    /// driven by Fusion; animating it would fight the network transform and read as jitter. The
    /// clips and this component do not collide either: every clip's curves target the armature
    /// (<c>*_chicken_rig</c> and the 18 bones below it), which is a <em>child</em> of the model
    /// root — no clip keys the model root itself.
    ///
    /// <b>Fallback.</b> If the avatar or the clips fail to bind, <see cref="SetSkeletalActive"/> is
    /// told so and the legacy fully-procedural path takes over, so a broken import degrades to a
    /// visibly animated chicken rather than a frozen one. The two paths are deliberately kept
    /// whole and separate rather than interleaved with per-effect conditionals.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenAnimator : MonoBehaviour
    {
        // ---- Animator parameter IDs (hashed once; this is a per-frame path) ----
        private static readonly int SpeedHash       = Animator.StringToHash("Speed");
        private static readonly int AbilityCastHash = Animator.StringToHash("AbilityCast");
        private static readonly int HitHash         = Animator.StringToHash("Hit");
        private static readonly int StunnedHash     = Animator.StringToHash("Stunned");

        [Header("Locomotion")]
        [Tooltip("Smoothing applied to the locomotion speed value. Higher = snappier.")]
        [Range(1f, 30f)]
        [SerializeField] private float _speedSmoothing = 12f;

        [Header("Lean")]
        [Tooltip("Forward pitch at full speed. Beyond ~15 it reads as falling over.")]
        [Range(0f, 30f)]
        [SerializeField] private float _leanMaxDegrees = 12f;

        [Tooltip("Banking roll into a turn, at the reference turn rate below.")]
        [Range(0f, 30f)]
        [SerializeField] private float _bankMaxDegrees = 10f;

        [Tooltip("Root yaw rate (deg/s) that produces a full-strength bank.")]
        [SerializeField] private float _bankReferenceTurnRate = 220f;

        // ---- Legacy procedural fallback -------------------------------------------------
        // Everything below this line is used ONLY when the skeletal path failed to bind
        // (see SetSkeletalActive). It is the pre-clip behaviour, kept whole so a broken rig
        // import degrades to "moving chicken" instead of "statue". Do NOT wire any of it into
        // the skeletal path — the clips already own every one of these beats.

        [Header("Fallback only — locomotion hop")]
        [Tooltip("Hop cycles per second at a standstill / at full speed. Two hops per cycle.")]
        [SerializeField] private Vector2 _hopFrequencyRange = new Vector2(1.1f, 2.6f);

        [Tooltip("Peak hop height in local units at full speed.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _hopHeight = 0.16f;

        [Tooltip("Squash on landing / stretch through the apex, at full speed.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float _hopSquashStretch = 0.14f;

        [Header("Fallback only — idle")]
        [Tooltip("Idle bob height. Biased upward only — never dips below the rest pose.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float _idleBobHeight = 0.045f;

        [Tooltip("Idle bob / breathing cycles per second.")]
        [SerializeField] private float _idleBobFrequency = 0.75f;

        [Tooltip("Breathing scale swell while idle.")]
        [Range(0f, 0.15f)]
        [SerializeField] private float _idleBreathAmount = 0.03f;

        [Header("Fallback only — hit reaction")]
        [Min(0.01f)]
        [SerializeField] private float _hitDuration = 0.26f;

        [Tooltip("Peak squash depth on impact.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _hitSquashDepth = 0.22f;

        [Tooltip("Backward recoil pitch on impact.")]
        [Range(0f, 40f)]
        [SerializeField] private float _hitRecoilDegrees = 16f;

        [Header("Fallback only — cast anticipation")]
        [Min(0.01f)]
        [SerializeField] private float _castDuration = 0.42f;

        [Tooltip("Fraction of the cast spent crouching before the pop.")]
        [Range(0.1f, 0.8f)]
        [SerializeField] private float _castAnticipationFraction = 0.35f;

        [Tooltip("Crouch depth during anticipation.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _castCrouchDepth = 0.18f;

        [Tooltip("Stretch height during the release pop.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _castPopHeight = 0.20f;

        [Header("Fallback only — stun")]
        [Tooltip("Seconds to blend fully into / out of the stunned pose.")]
        [SerializeField] private float _stunBlendSeconds = 0.18f;

        [Tooltip("How far the body sags while stunned.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _stunSagDepth = 0.16f;

        [Tooltip("Drunken sway amplitude while stunned.")]
        [Range(0f, 40f)]
        [SerializeField] private float _stunSwayDegrees = 13f;

        [Tooltip("Drunken sway cycles per second.")]
        [SerializeField] private float _stunSwayFrequency = 1.35f;

        // ---- Runtime state ----------------------------------------------------

        private ChickenController _controller;
        private Transform _modelRoot;

        // Resolved in Awake rather than serialized: Chicken.prefab still carries a stale
        // '_animator:' entry from an earlier version of this script, and relying on that ghost
        // rebinding to the right component is fragile.
        private Animator _animator;

        // Set by ChickenController once it knows whether the avatar + all five clips bound.
        // False until told otherwise, so a chicken whose controller never reports in still moves.
        private bool _skeletalActive;

        // The model's authored rest pose, captured when the model is handed over. Every effect
        // is an OFFSET from this. AttachClassModel derives localPosition.y at runtime from the
        // CharacterController capsule (roughly -0.8) — overwriting it instead of offsetting from
        // it sinks or floats every chicken.
        private Vector3    _restPosition = Vector3.zero;
        private Quaternion _restRotation = Quaternion.identity;
        private Vector3    _restScale    = Vector3.one;

        private Vector3 _previousPosition;
        private float   _smoothedSpeed01;
        private float   _previousYaw;
        private float   _smoothedYawRate01;

        private float _hopPhase;
        private float _idlePhase;
        private float _stunPhase;

        private float _hitTimer;   // counts down; <= 0 = inactive
        private float _castTimer;  // counts down; <= 0 = inactive
        private bool  _stunned;
        private float _stunBlend;

        /// <summary>
        /// True when the Animator is genuinely able to accept parameter writes. Unity logs a
        /// warning per call for a parameter set on an animator with no controller, which at
        /// per-frame rates floods the console.
        /// </summary>
        private bool AnimatorReady =>
            _animator != null && _animator.isActiveAndEnabled &&
            _animator.runtimeAnimatorController != null;

        private void Awake()
        {
            _controller       = GetComponent<ChickenController>();
            _animator         = GetComponent<Animator>();
            _previousPosition = transform.position;
            _previousYaw      = transform.eulerAngles.y;

            // Desynchronise the idle cycles so four chickens standing together do not breathe
            // and bob in lockstep, which reads as a single rigid object. (Fallback path only —
            // harmless to seed regardless.)
            _idlePhase = Random.value * Mathf.PI * 2f;
            _hopPhase  = Random.value * Mathf.PI * 2f;
        }

        /// <summary>
        /// Receives the per-class model that <see cref="ChickenController.Spawned"/> just parented
        /// under this chicken, and captures its authored rest pose as the origin for every offset.
        /// Mirrors <see cref="ChickenVisuals.SetModelRoot"/> — both are called from the same place
        /// for the same reason: <c>Awake</c> runs before <c>Spawned</c>, so the model does not
        /// exist yet and would otherwise never be animated.
        /// </summary>
        /// <remarks>
        /// Must be called with a model whose transform this component has not animated yet —
        /// i.e. exactly once per spawn, on a freshly instantiated model, which is what
        /// <c>ChickenController.Spawned</c> does. Calling it a second time on an already-animated
        /// model would capture a mid-squash pose as the new "rest" and drift.
        /// </remarks>
        public void SetModelRoot(Transform modelRoot)
        {
            _modelRoot = modelRoot;
            if (modelRoot == null) return;

            _restPosition = modelRoot.localPosition;
            _restRotation = modelRoot.localRotation;
            _restScale    = modelRoot.localScale;
        }

        /// <summary>
        /// Tells this component whether the Animator is really driving the skeleton. Called by
        /// <see cref="ChickenController"/> at spawn, right after it has tried to bind the class's
        /// avatar and its five clips.
        /// </summary>
        /// <param name="active">
        /// True when avatar AND all five clips bound. False switches this component to the legacy
        /// fully-procedural path so a broken import still produces a moving chicken.
        /// </param>
        public void SetSkeletalActive(bool active) => _skeletalActive = active;

        private void LateUpdate()
        {
            // A null model root means the class had no ModelPrefab. ChickenController.AttachClassModel
            // already logs that as an Error and the chicken is visibly missing, so reporting it again
            // here every frame would be a second source of truth for one authoring mistake — and a
            // per-frame log spam. Stay quiet and do nothing.
            if (_modelRoot == null || _controller == null || _controller.Stats == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateSpeed(dt);
            UpdateYawRate(dt);
            PushAnimatorParameters();

            if (_skeletalActive) ApplySkeletalOffsets();
            else                 ApplyLegacyProcedural(dt);
        }

        /// <summary>
        /// Speed and Stunned are pushed every frame rather than on change: <c>Animator.Rebind()</c>
        /// during spawn resets every parameter to its default, so a state set before that point
        /// would be silently lost. Triggers cannot be polled this way and stay event-driven.
        /// </summary>
        private void PushAnimatorParameters()
        {
            if (!AnimatorReady) return;

            _animator.SetFloat(SpeedHash, _smoothedSpeed01);
            _animator.SetBool(StunnedHash, _stunned);
        }

        /// <summary>
        /// The skeletal path. Lean and bank only — the clips own everything else. Contributes no
        /// vertical position offset and no scale at all, which is what makes the feet-on-ground
        /// invariant structural here.
        /// </summary>
        private void ApplySkeletalOffsets()
        {
            // ChickenMovement already rotates the ROOT to face the movement direction
            // (Quaternion.LookRotation on the root transform), so a plain local pitch is always
            // "forward" — no parent-space direction conversion needed. Banking rolls into the
            // turn the root is making.
            float pitch = _leanMaxDegrees * _smoothedSpeed01;
            float roll  = -_bankMaxDegrees * _smoothedYawRate01;

            // ---- The single write ----
            _modelRoot.localPosition = _restPosition;
            _modelRoot.localRotation = _restRotation * Quaternion.Euler(pitch, 0f, roll);
            _modelRoot.localScale    = _restScale;
        }

        /// <summary>
        /// The pre-clip behaviour, used only when the skeletal bind failed. Kept whole and
        /// unmodified so the degraded mode is a known quantity rather than a half-built variant.
        /// </summary>
        private void ApplyLegacyProcedural(float dt)
        {
            AdvanceLegacyTimers(dt);

            // ---- Accumulators. Nothing below writes the transform. ----
            float riseY = 0f;  // local units, always >= 0 (feet-on-ground invariant)
            float scaleY = 1f; // multiplicative squash/stretch, 1 = rest
            float pitch = 0f;  // degrees, + = nose down / forward
            float roll = 0f;   // degrees

            float moving = _smoothedSpeed01;
            float notStunned = 1f - _stunBlend; // stun suppresses hop and lean entirely

            // Idle bob + breathing. Hands over to the hop as speed comes up. Both curves run
            // 0 -> peak -> 0 across one full phase cycle, so they are continuous at the wrap.
            float idleWeight = (1f - moving) * notStunned;
            riseY  += _idleBobHeight * 0.5f * (1f - Mathf.Cos(_idlePhase)) * idleWeight;
            scaleY *= 1f + _idleBreathAmount * Mathf.Sin(_idlePhase * 0.5f) * idleWeight;

            // Locomotion hop. |sin| gives two bounces per cycle and is never negative, so the
            // arc only ever lifts. Squash at the landings, stretch through the apex.
            float arc = Mathf.Abs(Mathf.Sin(_hopPhase));
            float hopWeight = moving * notStunned;
            riseY  += _hopHeight * arc * hopWeight;
            scaleY *= 1f + _hopSquashStretch * (arc - 0.5f) * 2f * hopWeight;

            // Lean into movement, and bank into the turn.
            pitch += _leanMaxDegrees * moving * notStunned;
            roll  += -_bankMaxDegrees * _smoothedYawRate01 * notStunned;

            // Hit: instant-on squash impulse with a quick recoil, decaying to nothing.
            if (_hitTimer > 0f)
            {
                float env = _hitTimer / _hitDuration;
                scaleY *= 1f - _hitSquashDepth * env;
                pitch  -= _hitRecoilDegrees * env;
            }

            // Cast: crouch (anticipation) then pop (release). Continuous across the boundary —
            // the crouch decays to zero over the release while the pop rides on top.
            if (_castTimer > 0f)
            {
                float p = 1f - (_castTimer / _castDuration);
                float a = _castAnticipationFraction;
                float crouch;
                if (p < a)
                {
                    crouch = _castCrouchDepth * (p / a);
                }
                else
                {
                    float u = (p - a) / (1f - a);
                    crouch = Mathf.Lerp(_castCrouchDepth, 0f, u) - _castPopHeight * Mathf.Sin(u * Mathf.PI);
                }
                scaleY *= 1f - crouch;
            }

            // Stun: sag plus a slow drunken sway, so it reads from across the arena.
            if (_stunBlend > 0f)
            {
                scaleY *= 1f - _stunSagDepth * _stunBlend;
                roll   += Mathf.Sin(_stunPhase) * _stunSwayDegrees * _stunBlend;
                pitch  += Mathf.Cos(_stunPhase * 0.7f) * _stunSwayDegrees * 0.5f * _stunBlend;
            }

            // ---- The single write ----
            // Safety rail, not a tuning knob: several effects multiply into scaleY, and a
            // simultaneous hit + cast + stun could otherwise collapse or balloon the silhouette.
            scaleY = Mathf.Clamp(scaleY, 0.55f, 1.5f);
            float scaleXZ = 1f / Mathf.Sqrt(scaleY); // volume-preserving

            _modelRoot.localPosition = _restPosition + Vector3.up * riseY;
            _modelRoot.localRotation = _restRotation * Quaternion.Euler(pitch, 0f, roll);
            _modelRoot.localScale    = new Vector3(
                _restScale.x * scaleXZ,
                _restScale.y * scaleY,
                _restScale.z * scaleXZ);
        }

        private void UpdateSpeed(float dt)
        {
            var delta = transform.position - _previousPosition;
            delta.y = 0f;
            _previousPosition = transform.position;

            float instantSpeed = delta.magnitude / dt;
            float maxSpeed = Mathf.Max(0.01f, _controller.Stats.MoveSpeed);
            float target01 = Mathf.Clamp01(instantSpeed / maxSpeed);

            _smoothedSpeed01 = Mathf.Lerp(_smoothedSpeed01, target01, 1f - Mathf.Exp(-_speedSmoothing * dt));
        }

        private void UpdateYawRate(float dt)
        {
            float yaw = transform.eulerAngles.y;
            float rate = Mathf.DeltaAngle(_previousYaw, yaw) / dt;
            _previousYaw = yaw;

            float target = Mathf.Clamp(rate / Mathf.Max(1f, _bankReferenceTurnRate), -1f, 1f);
            // Deliberately shares _speedSmoothing rather than adding a second knob: the bank is a
            // subtle secondary effect and nobody has ever needed to smooth it differently.
            _smoothedYawRate01 = Mathf.Lerp(_smoothedYawRate01, target, 1f - Mathf.Exp(-_speedSmoothing * dt));
        }

        /// <summary>Phase/timer bookkeeping for the legacy path only.</summary>
        private void AdvanceLegacyTimers(float dt)
        {
            float hopFrequency = Mathf.Lerp(_hopFrequencyRange.x, _hopFrequencyRange.y, _smoothedSpeed01);
            _hopPhase  = Mathf.Repeat(_hopPhase + Mathf.PI * 2f * hopFrequency * dt, Mathf.PI * 2f);
            _idlePhase = Mathf.Repeat(_idlePhase + Mathf.PI * 2f * _idleBobFrequency * dt, Mathf.PI * 2f);
            _stunPhase = Mathf.Repeat(_stunPhase + Mathf.PI * 2f * _stunSwayFrequency * dt, Mathf.PI * 2f);

            if (_hitTimer > 0f) _hitTimer -= dt;
            if (_castTimer > 0f) _castTimer -= dt;

            float stunTarget = _stunned ? 1f : 0f;
            float step = _stunBlendSeconds > 0f ? dt / _stunBlendSeconds : 1f;
            _stunBlend = Mathf.MoveTowards(_stunBlend, stunTarget, step);
        }

        /// <summary>
        /// Fires the Cast state. Called by <see cref="AbilityController"/> when a slot fires.
        /// </summary>
        public void TriggerAbilityCast()
        {
            if (AnimatorReady) _animator.SetTrigger(AbilityCastHash);
            if (!_skeletalActive) _castTimer = _castDuration;
        }

        /// <summary>
        /// Fires the Hit state. Called by <see cref="HitFeedback.TriggerVictimHit"/>, which
        /// is the shared sink for every victim-impact beat (knockback edge, control-state entry,
        /// cargo loss, zone catch) and already does the baseline-seeded edge detection on the
        /// replicated state. Firing from there rather than re-detecting keeps one source of truth.
        /// </summary>
        public void TriggerHit()
        {
            if (AnimatorReady) _animator.SetTrigger(HitHash);
            if (!_skeletalActive) _hitTimer = _hitDuration;
        }

        /// <summary>Enters / leaves the Stunned state.</summary>
        /// <remarks>
        /// The field is set on both paths: the skeletal path reads it back out through
        /// <see cref="PushAnimatorParameters"/>, the legacy path blends it into
        /// <c>_stunBlend</c>. One setter, so the two can never disagree.
        /// </remarks>
        public void SetStunned(bool stunned) => _stunned = stunned;
    }
}
