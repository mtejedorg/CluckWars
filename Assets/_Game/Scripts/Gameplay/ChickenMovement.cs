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
    /// </remarks>
    public sealed class ChickenMovement
    {
        private readonly CharacterController _controller;
        private readonly ChickenController _owner;
        private readonly Transform _transform;

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

            // Planar input is ignored when movement is locked (Egg Shell, Turtle Mode, …).
            var planar = Vector3.zero;
            if (!_owner.MovementLocked)
            {
                planar = new Vector3(input.x, 0f, input.y);
                if (planar.sqrMagnitude > 1f) planar.Normalize();
            }

            // Gravity always runs so we stay grounded — even while locked.
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += Physics.gravity.y * deltaTime;

            var speed = stats.MoveSpeed * Mathf.Max(0f, _owner.MoveSpeedMultiplier);
            var displacement = planar * speed;
            displacement.y = _verticalVelocity;
            _controller.Move(displacement * deltaTime);

            // Face movement direction. Skip while locked or with no input.
            if (!_owner.MovementLocked && planar.sqrMagnitude > 0.01f)
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
