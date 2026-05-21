using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only bridge between the chicken's transform/state and its <see cref="Animator"/>.
    /// Runs on every client (state authority and proxies alike) — animation is never networked.
    /// Lives as a sibling component on the Chicken prefab.
    /// </summary>
    /// <remarks>
    /// Phase 2 drove only the locomotion blend; Phase 3 added <c>Hit</c> / <c>Stunned</c>
    /// hooks driven by <see cref="ChickenCombat"/>'s ChangeDetector so every peer sees
    /// the same reaction to networked HP / stun changes.
    ///
    /// v0.3: the <c>Attack</c> trigger (and its hash) have been replaced by
    /// <c>AbilityCast</c>. Fired from <see cref="AbilityController.TryActivate"/> on
    /// slot activation. Maestro must update the AnimatorController: delete the
    /// <c>Attack</c> parameter, add <c>AbilityCast</c> (trigger).
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenAnimator : MonoBehaviour
    {
        // Hashed once. Animator parameter strings live here, not scattered through callsites.
        public static readonly int SpeedHash       = Animator.StringToHash("Speed");
        public static readonly int AbilityCastHash = Animator.StringToHash("AbilityCast"); // v0.3 (replaces Attack)
        public static readonly int HitHash         = Animator.StringToHash("Hit");
        public static readonly int StunnedHash     = Animator.StringToHash("Stunned");

        [SerializeField] private Animator _animator;

        [Tooltip("Smoothing applied to the locomotion blend value. Higher = snappier.")]
        [Range(1f, 30f)]
        [SerializeField] private float _speedSmoothing = 12f;

        private ChickenController _controller;
        private Vector3 _previousPosition;
        private float _smoothedSpeed01;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            _previousPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (_animator == null || _controller == null || _controller.Stats == null) return;

            var delta = transform.position - _previousPosition;
            delta.y = 0f;
            _previousPosition = transform.position;

            var dt = Time.deltaTime;
            var instantSpeed = dt > 0f ? delta.magnitude / dt : 0f;
            var maxSpeed = Mathf.Max(0.01f, _controller.Stats.MoveSpeed);
            var target01 = Mathf.Clamp01(instantSpeed / maxSpeed);

            _smoothedSpeed01 = Mathf.Lerp(_smoothedSpeed01, target01, 1f - Mathf.Exp(-_speedSmoothing * dt));
            _animator.SetFloat(SpeedHash, _smoothedSpeed01);
        }

        /// <summary>
        /// Fires the <c>AbilityCast</c> trigger when the local or remote chicken
        /// activates an ability. Called by <see cref="AbilityController"/> on the
        /// state authority; on remote peers the animator reacts to the
        /// <c>[Networked] ActiveSlot</c> change via a future ChangeDetector hook
        /// (Phase 9 / Part B polish).
        /// </summary>
        public void TriggerAbilityCast()
        {
            if (_animator != null) _animator.SetTrigger(AbilityCastHash);
        }

        public void TriggerHit()
        {
            if (_animator != null) _animator.SetTrigger(HitHash);
        }

        public void SetStunned(bool stunned)
        {
            if (_animator != null) _animator.SetBool(StunnedHash, stunned);
        }
    }
}
