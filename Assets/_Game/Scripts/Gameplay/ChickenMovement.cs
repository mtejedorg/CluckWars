using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Pure-C# movement helper. No MonoBehaviour, no networking — just translates a
    /// 2D input vector into a <see cref="CharacterController.Move"/> displacement
    /// using stats from <see cref="ChickenStatsSO"/>. Owned by <see cref="ChickenController"/>
    /// and ticked from <c>FixedUpdateNetwork</c>.
    /// </summary>
    public sealed class ChickenMovement
    {
        private readonly CharacterController _controller;
        private readonly ChickenStatsSO _stats;
        private readonly Transform _transform;

        private float _verticalVelocity;

        public ChickenMovement(CharacterController controller, ChickenStatsSO stats)
        {
            _controller = controller;
            _stats = stats;
            _transform = controller.transform;
        }

        public void Tick(Vector2 input, float deltaTime)
        {
            // Treat input as world-space XZ. Camera-relative basis comes later.
            var planar = new Vector3(input.x, 0f, input.y);
            if (planar.sqrMagnitude > 1f) planar.Normalize();

            // Apply gravity so the controller stays grounded on uneven terrain.
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += Physics.gravity.y * deltaTime;

            var displacement = planar * _stats.MoveSpeed;
            displacement.y = _verticalVelocity;
            _controller.Move(displacement * deltaTime);

            // Face movement direction.
            if (planar.sqrMagnitude > 0.01f)
            {
                var targetRot = Quaternion.LookRotation(planar, Vector3.up);
                _transform.rotation = Quaternion.RotateTowards(
                    _transform.rotation,
                    targetRot,
                    _stats.TurnSpeed * deltaTime);
            }
        }
    }
}
