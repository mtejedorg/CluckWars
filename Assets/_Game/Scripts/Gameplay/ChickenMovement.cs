using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Pure-C# movement helper. No MonoBehaviour, no networking — just translates a
    /// 2D input vector into a <see cref="CharacterController.Move"/> displacement
    /// using stats + ability modifiers from the owning <see cref="ChickenController"/>.
    /// Owned by <see cref="ChickenController"/> and ticked from <c>FixedUpdateNetwork</c>.
    /// </summary>
    /// <remarks>
    /// Phase 6: reads <c>MoveSpeedMultiplier</c> and <c>MovementLocked</c> from the
    /// owner so abilities (Speed Burst, Egg Shell, …) can modulate movement without
    /// touching this class. Gravity always runs even when locked so a frozen
    /// chicken still stays grounded.
    ///
    /// v0.3: also reads <c>SlowMultiplier</c> (collision + pile + ability slow
    /// sources), <c>Rooted</c> (blocks planar motion but allows ability casts, unlike
    /// <c>MovementLocked</c>), and <c>ExternalDisplacement</c> (knockback impulse,
    /// decayed to zero exponentially each tick).
    /// </remarks>
    public sealed class ChickenMovement
    {
        private readonly CharacterController _controller;
        private readonly ChickenController _owner;
        private readonly Transform _transform;

        // ExternalDisplacement (knockback) decays toward zero at this rate (1/s).
        // Tune during the Part B balance pass. A value of 8 means the impulse
        // falls to ~1% of its initial value in about 0.58 seconds.
        private const float KnockbackDecayRate = 8f;

        private float _verticalVelocity;

        public ChickenMovement(CharacterController controller, ChickenController owner)
        {
            _controller = controller;
            _owner = owner;
            _transform = controller.transform;
        }

        public void Tick(Vector2 input, float deltaTime)
        {
            var stats = _owner.Stats;
            if (stats == null) return;

            // Planar input is ignored when movement is locked (Egg Shell, Turtle Mode, …)
            // OR when rooted (Rooted blocks horizontal motion but allows ability casts).
            var planar = Vector3.zero;
            if (!_owner.MovementLocked && !_owner.Rooted)
            {
                planar = new Vector3(input.x, 0f, input.y);
                if (planar.sqrMagnitude > 1f) planar.Normalize();
            }

            // Gravity always runs so we stay grounded — even while locked or rooted.
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += Physics.gravity.y * deltaTime;

            // Speed = base × ability multiplier × slow multiplier (all are ≥ 0).
            var speed = stats.MoveSpeed
                * Mathf.Max(0f, _owner.MoveSpeedMultiplier)
                * Mathf.Max(0f, _owner.SlowMultiplier);

            var displacement = planar * speed;
            displacement.y = _verticalVelocity;

            // External displacement (knockback / push) is a velocity in world-units/sec.
            // Applied additively, decays exponentially toward zero.
            if (_owner.ExternalDisplacement.sqrMagnitude > 0.001f)
            {
                displacement += _owner.ExternalDisplacement;
                _owner.ExternalDisplacement = Vector3.Lerp(
                    _owner.ExternalDisplacement, Vector3.zero, KnockbackDecayRate * deltaTime);
            }

            _controller.Move(displacement * deltaTime);

            // Face movement direction. Skip while locked, rooted, or with no input.
            if (!_owner.MovementLocked && !_owner.Rooted && planar.sqrMagnitude > 0.01f)
            {
                var targetRot = Quaternion.LookRotation(planar, Vector3.up);
                _transform.rotation = Quaternion.RotateTowards(
                    _transform.rotation,
                    targetRot,
                    stats.TurnSpeed * deltaTime);
            }
        }
    }
}
